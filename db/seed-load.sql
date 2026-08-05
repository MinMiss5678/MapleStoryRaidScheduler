-- =============================================================
-- Load-test 專用 seed（可重複執行、單一 period，可跟 seed-e2e.sql 一樣 TRUNCATE）
--
-- 目的：plans/2026-07-28-load-testing.md Phase 1 —— 對同一 period 灌 N 個「尚未報名」
-- 的獨立玩家，讓 k6 逐一打 POST /api/register，真正對 TeamSlotAutoAssignService 的
-- classId 1001 advisory lock 製造併發（不是序列呼叫）。
--
-- 跟 seed-e2e.sql 的差異：這裡的玩家/角色「不」預先報名——報名本身就是 k6 要做的事。
-- N 用 generate_series 產生，改這裡的 60 就能調併發規模。
--
-- 用法：docker compose -f compose.e2e.yaml exec -T e2e-db env PGPASSWORD=e2e \
--        psql -U postgres -d presentationdb < db/seed-load.sql
-- =============================================================

BEGIN;

TRUNCATE "TeamSlotCharacter","TeamSlot","CharacterRegister","PlayerAvailability",
         "PlayerRegister","Character","Player","BossTemplateRequirement","BossTemplate",
         "Boss","Period","JobCategory"
    RESTART IDENTITY CASCADE;

DO $$
DECLARE
    v_period_start date;
    v_period_id   int;
    v_boss_id     int;
    v_template_id int;
    v_weekday     int         := 2;   -- 對齊 SlotDateCalculator.ResetDay=Tuesday（System.DayOfWeek 編號 2）
    v_n           int         := 500; -- 併發玩家數上限，k6 VUS 用這個範圍內的子集
    i             int;
    v_discord_id  bigint;
BEGIN
    -- 🔴 period.StartDate 必須落在「重製日」（SlotDateCalculator.ResetDay=Tuesday）——
    -- CycleDayOffset 的整套日期換算都假設 period 從週二開始；用任意 CURRENT_DATE+N 當
    -- StartDate（不檢查落在哪個星期幾）會讓 FindMatchingTeam 重新從 SlotDateTime 算回的
    -- 星期幾，跟建隊當下用 avail.Weekday 算的 CycleDayOffset 對不上——兩邊都各自「沒錯」，
    -- 但因為基準日不是週二而互相兜不起來，結果是每個人都被判定「沒有相符的隊」、各自開新隊，
    -- 明明同一王同一時段，卻永遠不會合併（load 測試 Phase 1 真的踩到過這個）。
    v_period_start := (CURRENT_DATE + 10) + ((2 - EXTRACT(DOW FROM (CURRENT_DATE + 10))::int + 7) % 7);

    -- 唯一當期（未來一週，StartDate 保證是週二）→ GetActivePeriodAsync 回這個，且報名截止日在未來 → 報名開著
    INSERT INTO "Period"("StartDate","EndDate")
    VALUES (v_period_start::timestamptz, (v_period_start + 7)::timestamptz)
    RETURNING "Id" INTO v_period_id;

    -- 單一王（RequireMembers=6）→ N=60 人報名會分裝成約 10 支隊，剛好反覆踩「讀現有隊→沒有就開新隊」
    INSERT INTO "Boss"("Name","RequireMembers","RoundConsumption")
    VALUES ('Load王', 6, 1) RETURNING "Id" INTO v_boss_id;
    INSERT INTO "BossTemplate"("BossId","Name")
    VALUES (v_boss_id, 'Load範本') RETURNING "Id" INTO v_template_id;
    INSERT INTO "BossTemplateRequirement"("BossTemplateId","JobCategory","Count","Priority")
    VALUES (v_template_id, '輸出', 6, 1);
    INSERT INTO "JobCategory"("JobName","CategoryName") VALUES ('Hero', '輸出');

    -- N 個玩家 + 各一支角色，discordId = 9000001..9000000+N，characterId = 'l1'..'lN'（Id 上限 5，前綴縮成 1 字）
    -- 全部「未報名」——報名這個動作留給 k6 去做，才是真的在測併發寫入路徑。
    FOR i IN 1..v_n LOOP
        v_discord_id := 9000000 + i;
        INSERT INTO "Player"("DiscordId","DiscordName","Role")
        VALUES (v_discord_id, 'Load' || i, 'user');
        INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
        VALUES ('l' || i, v_discord_id, 'CLoad' || i, 'Hero', 900 + i);
    END LOOP;

    RAISE NOTICE 'Load seed 就緒 → periodId=%, bossId=%, weekday=%, players=1..%（discordId 9000001..%)',
        v_period_id, v_boss_id, v_weekday, v_n, 9000000 + v_n;
END $$;

COMMIT;
