-- ============================================================================
-- Demo 種子：拉圖斯 leader-led + 組成配額(OR 群組) + 招募熱力圖 + Discord DM 錄影用
-- ----------------------------------------------------------------------------
-- 情境：隊長開「拉圖斯」隊(容量 6)，開兩個需求列(OR 群組)：
--   Row1 法師群：主教 / 火毒大魔導士 / 冰雷大魔導士
--   Row2 敏系群：夜使者 / 箭神 / 槍神
-- 候選池 9 人(4 法師群 + 5 敏系群)全參戰、全週晚上有空 → 候選滿、熱力圖亮。
--
-- ⚠️ 錄 DM 用：被邀者必須是「真 Discord 公會成員」→ 這裡設成【你的帳號 356046901914894337】。
--    隊長請用「另一個身分」登入(2nd Discord 帳號，或 Dev 模式 test-login)，去邀「你(夜使者)」→
--    你的真 Discord 會收到含按鈕的 DM → 錄「接受」→ 網頁 + DM 兩邊即時更新。
--
-- 錄影前把需求列的「場次/通關數(MinClearCount)」設 0（或 ≤15，已 seed 15 場），
-- 攻擊力門檻設 ≤1500，才不會把候選過濾掉。
--
-- 載入(本機 docker compose 主 stack)：
--   docker compose exec -T -e PGPASSWORD=<db密碼> database \
--     psql -U postgres -d presentationdb < db/seed-demo-lotus.sql
-- ============================================================================
BEGIN;

-- 清舊 demo 資料(重跑安全)。假帳號用 900001-900099；拉圖斯依名字清。
DELETE FROM "Player" WHERE "DiscordId" BETWEEN 900001 AND 900099;  -- CASCADE 連帶清其角色/時段/通關
DELETE FROM "Boss"   WHERE "Name" = '拉圖斯';

-- ── Boss ──
INSERT INTO "Boss" ("Name", "RequireMembers", "RoundConsumption") VALUES ('拉圖斯', 6, 1);

-- ── 玩家(假候選) ──
INSERT INTO "Player" ("DiscordId", "DiscordName", "Role") VALUES
  (900001, '光之祭司', 'user'), (900002, '烈焰魔導', 'user'), (900003, '極凍魔導', 'user'),
  (900004, '暗夜刺客', 'user'), (900005, '疾風神箭', 'user'), (900006, '重砲槍神', 'user'),
  (900007, '聖光主教', 'user'), (900008, '影襲夜行', 'user');

-- ── 玩家(被邀者 = 你的真帳號) ──
INSERT INTO "Player" ("DiscordId", "DiscordName", "Role") VALUES
  (356046901914894337, '★你(受邀者)', 'user')
ON CONFLICT ("DiscordId") DO UPDATE SET "DiscordName" = EXCLUDED."DiscordName";

-- ── 角色(全參戰) ──
--   Row1 法師群：主教×2 + 火毒 + 冰雷；Row2 敏系群：夜使者×2 + 箭神 + 槍神 + 【你 夜使者】
INSERT INTO "Character" ("Id","DiscordId","Name","Job","AttackPower","Level","IsSeekingRaid","MapleBlessingLevel") VALUES
  ('demo01', 900001, '光之祭司', '主教',         1450, 200, true, 30),
  ('demo02', 900002, '烈焰魔導', '火毒大魔導士', 1520, 200, true, 28),
  ('demo03', 900003, '極凍魔導', '冰雷大魔導士', 1480, 198, true, 26),
  ('demo07', 900007, '聖光主教', '主教',         1400, 195, true, 22),
  ('demo04', 900004, '暗夜刺客', '夜使者',       1550, 200, true, 30),
  ('demo05', 900005, '疾風神箭', '箭神',         1500, 199, true, 27),
  ('demo06', 900006, '重砲槍神', '槍神',         1470, 197, true, 26),
  ('demo08', 900008, '影襲夜行', '夜使者',       1490, 196, true, 25);
INSERT INTO "Character" ("Id","DiscordId","Name","Job","AttackPower","Level","IsSeekingRaid","MapleBlessingLevel") VALUES
  ('demoYou', 356046901914894337, '★你-夜使者', '夜使者', 1600, 200, true, 30)
ON CONFLICT ("Id") DO UPDATE SET
  "Job"='夜使者', "IsSeekingRaid"=true, "DiscordId"=356046901914894337, "AttackPower"=1600, "Level"=200;

-- ── 常設可用時段：所有候選全週(0-6)晚上 20:00-23:00 → 熱力圖各日 20/21/22 點亮綠 ──
INSERT INTO "PlayerAvailabilityStanding" ("DiscordId","Weekday","StartTime","EndTime")
SELECT d, wd, TIME '20:00', TIME '23:00'
FROM (VALUES (900001),(900002),(900003),(900004),(900005),(900006),(900007),(900008),(356046901914894337)) v(d),
     generate_series(0, 6) wd;
-- 少數候選加平日午後(14-17) → 熱力圖午後有較淡的一塊(gradient，畫面更有層次)
INSERT INTO "PlayerAvailabilityStanding" ("DiscordId","Weekday","StartTime","EndTime")
SELECT d, wd, TIME '14:00', TIME '17:00'
FROM (VALUES (900001),(900004),(900005)) v(d), generate_series(1, 5) wd;

-- ── 拉圖斯通關數(每人 15 場) → 隊長把 MinClearCount 設 ≤15 也不會過濾掉 ──
INSERT INTO "CharacterBossClear" ("CharacterId","BossId","ClearCount")
SELECT c."Id", (SELECT "Id" FROM "Boss" WHERE "Name"='拉圖斯'), 15
FROM "Character" c
WHERE c."Id" IN ('demo01','demo02','demo03','demo04','demo05','demo06','demo07','demo08','demoYou');

COMMIT;

-- 驗證
SELECT '候選(參戰+有拉圖斯場次)' AS chk, count(*) FROM "Character" WHERE "IsSeekingRaid";
