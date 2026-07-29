-- =============================================================
-- Load-test 專用 seed（TeamSlot 編輯鎖，Phase 2）
--
-- 目的：plans/2026-07-28-load-testing.md §1b —— 對同一 teamSlotId 灌 N 個併發「補位」
-- （PUT /api/teamslot，填既有空位），量 TeamSlotService.UpdateAsync 取 classId 1002
-- advisory lock 的正常排隊等待時間，跟 lock_timeout 預設 5s 比較。
--
-- 跟 register-load 不同：這裡每人填「不同」的空位（不同 TeamSlotCharacter.Id），
-- 刻意避開樂觀鎖版本衝突（那是另一個機制），純粹量悲觀鎖排隊本身的延遲。
--
-- 用法：docker compose -f compose.e2e.yaml exec -T e2e-db env PGPASSWORD=e2e \
--        psql -U postgres -d presentationdb < db/seed-load-teamslot.sql
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
    v_team_id     int;
    v_n           int := 600; -- 空位數上限，k6 VUS 用這個範圍內的子集
    i             int;
    v_discord_id  bigint;
BEGIN
    -- period 只需要涵蓋 TeamSlot.SlotDateTime；這裡不跑 AutoAssign，不需要對齊週二重製日。
    INSERT INTO "Period"("StartDate","EndDate")
    VALUES ((CURRENT_DATE + 10)::timestamptz, (CURRENT_DATE + 17)::timestamptz)
    RETURNING "Id" INTO v_period_id;

    INSERT INTO "Boss"("Name","RequireMembers","RoundConsumption")
    VALUES ('LoadEdit王', v_n, 1) RETURNING "Id" INTO v_boss_id;

    INSERT INTO "TeamSlot"("BossId","SlotDateTime","Source")
    VALUES (v_boss_id, (CURRENT_DATE + 11)::timestamptz, 'auto') RETURNING "Id" INTO v_team_id;

    -- N 個空位（CharacterId 為 null）——每個 VU 填一個，不重疊
    FOR i IN 1..v_n LOOP
        INSERT INTO "TeamSlotCharacter"("TeamSlotId","Job") VALUES (v_team_id, '-');
    END LOOP;

    -- N 個玩家 + 各一支角色，discordId = 9100001..9100000+N，characterId = 'edit1'..'editN'
    FOR i IN 1..v_n LOOP
        v_discord_id := 9100000 + i;
        INSERT INTO "Player"("DiscordId","DiscordName","Role")
        VALUES (v_discord_id, 'Edit' || i, 'user');
        INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
        VALUES ('edit' || i, v_discord_id, 'CEdit' || i, 'Hero', 900 + i);
    END LOOP;

    RAISE NOTICE 'Load-teamslot seed 就緒 → periodId=%, bossId=%, teamSlotId=%, 空位=1..%（discordId 9100001..%)',
        v_period_id, v_boss_id, v_team_id, v_n, 9100000 + v_n;
END $$;

COMMIT;
