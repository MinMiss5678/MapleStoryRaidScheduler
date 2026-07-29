-- Phase 1 驗收查詢：接在 k6 register-load.js 跑完之後執行。
-- 用法：docker compose -f compose.e2e.yaml exec -T e2e-db env PGPASSWORD=e2e \
--        psql -U postgres -d presentationdb < db/verify-load.sql

-- 1) 報名成功的玩家數（應等於 k6 VUS）
SELECT count(*) AS registered_players FROM "PlayerRegister";

-- 2) 隊伍數與人數分佈（正確性核心：每隊人數應 <= Boss.RequireMembers，不能超編）
SELECT ts."Id" AS team_slot_id, count(tsc."Id") AS member_count, b."RequireMembers"
FROM "TeamSlot" ts
JOIN "Boss" b ON b."Id" = ts."BossId"
LEFT JOIN "TeamSlotCharacter" tsc ON tsc."TeamSlotId" = ts."Id" AND tsc."CharacterId" IS NOT NULL
GROUP BY ts."Id", b."RequireMembers"
ORDER BY ts."Id";

-- 3) 硬條件：有沒有任何隊超編（應該是 0 筆）
SELECT ts."Id", count(tsc."Id") AS member_count, b."RequireMembers"
FROM "TeamSlot" ts
JOIN "Boss" b ON b."Id" = ts."BossId"
JOIN "TeamSlotCharacter" tsc ON tsc."TeamSlotId" = ts."Id" AND tsc."CharacterId" IS NOT NULL
GROUP BY ts."Id", b."RequireMembers"
HAVING count(tsc."Id") > b."RequireMembers";

-- 4) 硬條件：有沒有玩家的角色被分進兩支不同隊（不該發生，應該是 0 筆）
SELECT "CharacterId", count(DISTINCT "TeamSlotId") AS team_count
FROM "TeamSlotCharacter"
WHERE "CharacterId" IS NOT NULL
GROUP BY "CharacterId"
HAVING count(DISTINCT "TeamSlotId") > 1;

-- 5) 總覽：入隊角色總數應等於 registered_players（沒有人漏派）
SELECT count(*) AS assigned_characters FROM "TeamSlotCharacter" WHERE "CharacterId" IS NOT NULL;
