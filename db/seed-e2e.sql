-- =============================================================
-- E2E 專用 seed（可重複執行、單一 period）
--
-- 為什麼要自成一份、且 TRUNCATE：
--   GetByNowAsync() 回傳的是「StartDate 最新的 period」（rolling-week 模型），
--   不是「含今天的 period」。backend 的 WeeklyPeriodJob 開機會自動建一個
--   未來 period，會蓋過 seed 的當期 → GetByDiscordId 撈不到隊。
--   → 這裡 TRUNCATE 掉所有交易資料，確保「seed 的當期」是唯一/最新的 period。
--
-- 建立最小情境：當期 + E2E王 + 玩家 P1(discordId 1002, 角色 c1002) 在一支自動隊。
-- 用法：docker compose -f compose.e2e.yaml exec -T e2e-db env PGPASSWORD=e2e \
--        psql -U postgres -d presentationdb < db/seed-e2e.sql
-- =============================================================

BEGIN;

TRUNCATE "TeamSlotCharacter","TeamSlot","CharacterRegister","PlayerAvailability",
         "PlayerRegister","Character","Player","BossTemplateRequirement","BossTemplate",
         "Boss","Period","JobCategory"
    RESTART IDENTITY CASCADE;

DO $$
DECLARE
    v_period_id   int;
    v_boss_id     int;
    v_template_id int;
    v_reg         int;
    v_team_id     int;
    v_boss2_id    int;
    v_tpl2_id     int;
    v_team2_id    int;
    v_boss3_id    int;
    v_tpl3_id     int;
    v_reg3        int;
    v_boss4_id    int;
    v_tpl4_id     int;
    v_team4_id    int;
    v_reg_cand    int;
    v_boss_full_id int;
    v_reg_full    int;
    -- period 設「未來一週」：截止日永遠在 period 開始前一週（GetDeadlineForPeriod 的 -7），
    -- StartDate 要夠遠（+10）截止日才落在未來、報名才開著（符合 app「當前=即將開打的下週」模型）
    v_weekday     int         := EXTRACT(DOW FROM (CURRENT_DATE + 11))::int;
    v_slot_ts     timestamptz := ((CURRENT_DATE + 11)::text || ' 12:00:00+00')::timestamptz; -- 未來週內某日 20:00（台北）
BEGIN
    -- 唯一當期（未來一週）→ GetActivePeriodAsync 回這個，且報名截止日在未來 → 報名開著
    INSERT INTO "Period"("StartDate","EndDate")
    VALUES ((CURRENT_DATE + 10)::timestamptz, (CURRENT_DATE + 17)::timestamptz)
    RETURNING "Id" INTO v_period_id;

    -- 王 + 範本（單一「輸出」、需 6 人）+ 職業對照
    INSERT INTO "Boss"("Name","RequireMembers","RoundConsumption")
    VALUES ('E2E王', 6, 1) RETURNING "Id" INTO v_boss_id;
    INSERT INTO "BossTemplate"("BossId","Name")
    VALUES (v_boss_id, 'E2E範本') RETURNING "Id" INTO v_template_id;
    INSERT INTO "BossTemplateRequirement"("BossTemplateId","JobCategory","Count","Priority")
    VALUES (v_template_id, '輸出', 6, 1);
    INSERT INTO "JobCategory"("JobName","CategoryName") VALUES ('Hero', '輸出');

    -- 玩家 P1 + 角色 + 報名 + 可用時段
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (1002, 'P1', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
    VALUES ('c1002', 1002, 'C1', 'Hero', 990);
    INSERT INTO "PlayerRegister"("DiscordId","PeriodId")
    VALUES (1002, v_period_id) RETURNING "Id" INTO v_reg;
    INSERT INTO "CharacterRegister"("PlayerRegisterId","CharacterId","BossId","Rounds")
    VALUES (v_reg, 'c1002', v_boss_id, 1);
    INSERT INTO "PlayerAvailability"("PlayerRegisterId","Weekday","StartTime","EndTime")
    VALUES (v_reg, v_weekday, TIME '20:00', TIME '22:00');

    -- 一支自動隊，P1 已在裡面（供讀取測試「看到自己的隊」）
    INSERT INTO "TeamSlot"("BossId","SlotDateTime","Source","TemplateId")
    VALUES (v_boss_id, v_slot_ts, 'auto', v_template_id) RETURNING "Id" INTO v_team_id;
    INSERT INTO "TeamSlotCharacter"
        ("TeamSlotId","DiscordId","DiscordName","CharacterId","CharacterName","Job","AttackPower","Rounds","IsManual")
    VALUES
        (v_team_id, 1002, 'P1', 'c1002', 'C1', 'Hero', 990, 1, false);

    -- 另一個玩家 P-New(2001)：有角色、尚未報名/未入隊 → 供「報名 → 自動排隊」寫入測試
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (2001, 'P-New', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
    VALUES ('c2001', 2001, 'CNew', 'Hero', 950);

    -- 第二隻王 E2E王2 + 只有 1 人的隊（5 個「輸出」空缺）→ 供「補位」測試。
    -- 用獨立王/隊，與 E2E王（報名/讀取測試用）隔離，避免平行測試互相干擾。
    INSERT INTO "Boss"("Name","RequireMembers","RoundConsumption") VALUES ('E2E王2', 6, 1) RETURNING "Id" INTO v_boss2_id;
    INSERT INTO "BossTemplate"("BossId","Name") VALUES (v_boss2_id, 'E2E範本2') RETURNING "Id" INTO v_tpl2_id;
    INSERT INTO "BossTemplateRequirement"("BossTemplateId","JobCategory","Count","Priority") VALUES (v_tpl2_id, '輸出', 6, 1);
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (4001, 'P-Dummy', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower") VALUES ('c4001', 4001, 'CDummy', 'Hero', 900);
    INSERT INTO "TeamSlot"("BossId","SlotDateTime","Source","TemplateId")
    VALUES (v_boss2_id, v_slot_ts, 'auto', v_tpl2_id) RETURNING "Id" INTO v_team2_id;
    INSERT INTO "TeamSlotCharacter"
        ("TeamSlotId","DiscordId","DiscordName","CharacterId","CharacterName","Job","AttackPower","Rounds","IsManual")
    VALUES (v_team2_id, 4001, 'P-Dummy', 'c4001', 'CDummy', 'Hero', 900, 0, false); -- Rounds=0：無場數限制，補位者(未報名 rounds=0)才過 validate #5

    -- 補位者 P-Fill(3001)：有「輸出」角色、未入隊 → 補位進 E2E王2 的空缺
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (3001, 'P-Fill', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower") VALUES ('c3001', 3001, 'CFill', 'Hero', 940);

    -- 第三隻王 E2E王3 + 範本 + 一個報名的池玩家 → 供「管理員重排」測試（獨立王隔離）
    INSERT INTO "Boss"("Name","RequireMembers","RoundConsumption") VALUES ('E2E王3', 6, 1) RETURNING "Id" INTO v_boss3_id;
    INSERT INTO "BossTemplate"("BossId","Name") VALUES (v_boss3_id, 'E2E範本3') RETURNING "Id" INTO v_tpl3_id;
    INSERT INTO "BossTemplateRequirement"("BossTemplateId","JobCategory","Count","Priority") VALUES (v_tpl3_id, '輸出', 6, 1);
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (5001, 'P-Pool', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower") VALUES ('c5001', 5001, 'CPool', 'Hero', 930);
    INSERT INTO "PlayerRegister"("DiscordId","PeriodId") VALUES (5001, v_period_id) RETURNING "Id" INTO v_reg3;
    INSERT INTO "CharacterRegister"("PlayerRegisterId","CharacterId","BossId","Rounds") VALUES (v_reg3, 'c5001', v_boss3_id, 1);
    INSERT INTO "PlayerAvailability"("PlayerRegisterId","Weekday","StartTime","EndTime") VALUES (v_reg3, v_weekday, TIME '20:00', TIME '22:00');

    -- 第四隻王 E2E王4 + 只有 1 人的隊 → 供「管理員存檔衝突」測試（獨立王/隊隔離，
    -- 不可跟 E2E王2 共用：admin-conflict 測試會把這隻隊的最後一人移除、連帶砍團，
    -- 若跟補位測試共用同一隊，平行測試會互踩）。
    INSERT INTO "Boss"("Name","RequireMembers","RoundConsumption") VALUES ('E2E王4', 6, 1) RETURNING "Id" INTO v_boss4_id;
    INSERT INTO "BossTemplate"("BossId","Name") VALUES (v_boss4_id, 'E2E範本4') RETURNING "Id" INTO v_tpl4_id;
    INSERT INTO "BossTemplateRequirement"("BossTemplateId","JobCategory","Count","Priority") VALUES (v_tpl4_id, '輸出', 6, 1);
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (4002, 'P-Dummy2', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower") VALUES ('c4002', 4002, 'CDummy2', 'Hero', 900);
    INSERT INTO "TeamSlot"("BossId","SlotDateTime","Source","TemplateId")
    VALUES (v_boss4_id, v_slot_ts, 'auto', v_tpl4_id) RETURNING "Id" INTO v_team4_id;
    INSERT INTO "TeamSlotCharacter"
        ("TeamSlotId","DiscordId","DiscordName","CharacterId","CharacterName","Job","AttackPower","Rounds","IsManual")
    VALUES (v_team4_id, 4002, 'P-Dummy2', 'c4002', 'CDummy2', 'Hero', 900, 0, false);

    -- leader-led（隊長主導）Push 流程 e2e：申請者 P-LL(6002) + 角色 c6002（獨立、不與其他測試共用）。
    -- 隊長由 test-login 於測試中自建 Player（6001），故不必在此 seed。開隊/申請/審核全走 UI，
    -- 用專屬王時段（測試自選當期內時間）→ 不依賴上面的 auto 隊，也不干擾其他 spec。
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (6002, 'P-LL', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
    VALUES ('c6002', 6002, 'C-LL', '英雄', 950);

    -- leader-led 候選(Pull) e2e：候選 P-Cand(6003) 角色 c6003（中文職業「英雄」＝隊長 builder 選得到）
    -- + 報名（進候選池）+ 全週 7 天整天可用時段（00:00–00:00＝整天）→ 讓候選配對不吃團時段的星期/時區，
    -- 任何當期內的開團時間都命中。隊長由 test-login 自建（6004）。供 開隊→挑候選→邀請→玩家接受 流程。
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (6003, 'P-Cand', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
    VALUES ('c6003', 6003, 'C-Cand', '英雄', 950);
    INSERT INTO "PlayerRegister"("DiscordId","PeriodId") VALUES (6003, v_period_id) RETURNING "Id" INTO v_reg_cand;
    INSERT INTO "CharacterRegister"("PlayerRegisterId","CharacterId","BossId","Rounds")
    VALUES (v_reg_cand, 'c6003', v_boss_id, 1);
    INSERT INTO "PlayerAvailability"("PlayerRegisterId","Weekday","StartTime","EndTime")
    SELECT v_reg_cand, gs, TIME '00:00', TIME '00:00' FROM generate_series(1, 7) AS gs;

    -- leader-led 自動撤銷過期邀請(Tier 3) e2e：容量 1 的王「E2E王滿」+ 兩名全週可用候選
    -- P-Full-A(6005,c6005) / P-Full-B(6006,c6006)。隊長（6007,test-login 自建）邀兩人 → 其一接受使隊伍額滿
    -- → 另一人的邀請被自動撤銷（從其待處理邀請消失）。獨立王隔離、不與其他測試互撞。
    INSERT INTO "Boss"("Name","RequireMembers","RoundConsumption") VALUES ('E2E王滿', 1, 1) RETURNING "Id" INTO v_boss_full_id;
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (6005, 'P-Full-A', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower") VALUES ('c6005', 6005, 'C-Full-A', '夜使者', 950);
    INSERT INTO "PlayerRegister"("DiscordId","PeriodId") VALUES (6005, v_period_id) RETURNING "Id" INTO v_reg_full;
    INSERT INTO "CharacterRegister"("PlayerRegisterId","CharacterId","BossId","Rounds") VALUES (v_reg_full, 'c6005', v_boss_full_id, 1);
    INSERT INTO "PlayerAvailability"("PlayerRegisterId","Weekday","StartTime","EndTime")
    SELECT v_reg_full, gs, TIME '00:00', TIME '00:00' FROM generate_series(1, 7) AS gs;

    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (6006, 'P-Full-B', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower") VALUES ('c6006', 6006, 'C-Full-B', '夜使者', 950);
    INSERT INTO "PlayerRegister"("DiscordId","PeriodId") VALUES (6006, v_period_id) RETURNING "Id" INTO v_reg_full;
    INSERT INTO "CharacterRegister"("PlayerRegisterId","CharacterId","BossId","Rounds") VALUES (v_reg_full, 'c6006', v_boss_full_id, 1);
    INSERT INTO "PlayerAvailability"("PlayerRegisterId","Weekday","StartTime","EndTime")
    SELECT v_reg_full, gs, TIME '00:00', TIME '00:00' FROM generate_series(1, 7) AS gs;

    -- leader-led 隊長轉讓 e2e：申請者/未來新隊長 P-Trans(7002) + 角色 c7002（獨立、Push 流程用來入隊後被轉讓）。
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (7002, 'P-Trans', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
    VALUES ('c7002', 7002, 'C-Trans', '英雄', 950);

    RAISE NOTICE 'E2E seed 就緒 → periodId=%, bossId=%, teamId=%', v_period_id, v_boss_id, v_team_id;
END $$;

-- period-less Phase 2a：候選池改讀「常設可用時段 + 角色 IsSeekingRaid」（不再吃 period 報名）。
-- 把上面 seed 的報名資料鏡射過去，讓候選相關 e2e 在新查詢下維持同一候選集。
INSERT INTO "PlayerAvailabilityStanding"("DiscordId","Weekday","StartTime","EndTime")
SELECT pr."DiscordId", pa."Weekday", pa."StartTime", pa."EndTime"
FROM "PlayerAvailability" pa
JOIN "PlayerRegister" pr ON pr."Id" = pa."PlayerRegisterId";

UPDATE "Character" SET "IsSeekingRaid" = true
WHERE "Id" IN (SELECT DISTINCT "CharacterId" FROM "CharacterRegister");

COMMIT;
