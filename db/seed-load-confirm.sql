-- =============================================================
-- Load-test 專用 seed（入隊定案鎖，Phase 2；period-less 重指版）
--
-- 目的：plans/2026-07-28-load-testing.md §1b —— 對同一 teamSlotId 灌 N 個併發「接受邀請」
-- （PUT /api/teamSlot/{id}/Invitations/{memberId} action=accept），量 TeamLeaderService.ConfirmMemberAsync
-- 取 classId 1002 advisory lock（AcquireTeamSlotEditLockAsync）的正常排隊等待時間，跟 lock_timeout 預設 5s 比較。
--
-- （原 Phase 2 打 PUT /api/teamslot 補位，該端點與 TeamSlotService 已於 period-less 4c-be 退場；
--   同一把 classId 1002 鎖搬到了 ConfirmMemberAsync，故重指到這條現行熱路徑。）
--
-- 設計：容量 = N → 全部 accept 都成功、不觸發「隊伍已滿」→ 純量悲觀鎖排隊本身的延遲（比照舊版無衝突設計）。
-- 每個玩家接受「自己那一筆」邀請（memberId = TeamSlotCharacter.Id = 1..N，RESTART IDENTITY 後照插入序）。
--
-- 用法：docker compose -f compose.e2e.yaml exec -T e2e-db env PGPASSWORD=e2e \
--        psql -U postgres -d presentationdb < db/seed-load-confirm.sql
-- =============================================================

BEGIN;

TRUNCATE "TeamSlotCharacter","TeamSlot","Character","Player","Boss"
    RESTART IDENTITY CASCADE;

DO $$
DECLARE
    v_boss_id int;
    v_team_id int;
    v_slot    timestamptz := (CURRENT_DATE + 11)::timestamptz;  -- 未來時段（SlotDateTime 快照用同一值）
    v_n       int := 600;   -- 邀請數上限，k6 VUS 取此範圍內子集
    i         int;
    v_did     bigint;
BEGIN
    -- 容量 = N → 全部 accept 成功，量純鎖排隊延遲（超編硬條件由整合測守，不在此壓）
    INSERT INTO "Boss"("Name","RequireMembers","RoundConsumption")
    VALUES ('LoadConfirm王', v_n, 1) RETURNING "Id" INTO v_boss_id;

    -- 隊長（9100000）+ 一支 leader 排程隊（teamSlotId=1）
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (9100000, 'LoadLeader', 'user');
    INSERT INTO "TeamSlot"("BossId","SlotDateTime","Source","Kind","LeaderDiscordId")
    VALUES (v_boss_id, v_slot, 'leader', 'Scheduled', 9100000) RETURNING "Id" INTO v_team_id;

    -- N 名玩家 + 角色 + 各一筆「Invited」成員（同隊、SlotDateTime 快照 = 團時間；memberId = 1..N）
    FOR i IN 1..v_n LOOP
        v_did := 9100000 + i;
        INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (v_did, 'Cand' || i, 'user');
        INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
        VALUES ('e' || i, v_did, 'CE' || i, 'Hero', 900 + i);
        INSERT INTO "TeamSlotCharacter"
            ("TeamSlotId","DiscordId","DiscordName","CharacterId","CharacterName","Job","AttackPower","Status","SlotDateTime")
        VALUES (v_team_id, v_did, 'Cand' || i, 'e' || i, 'CE' || i, 'Hero', 900 + i, 'Invited', v_slot);
    END LOOP;

    RAISE NOTICE 'Load-confirm seed 就緒 → bossId=%, teamSlotId=%, 邀請 memberId=1..%（discordId 9100001..%)',
        v_boss_id, v_team_id, v_n, 9100000 + v_n;
END $$;

COMMIT;
