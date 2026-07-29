// Phase 2（plans/2026-07-28-load-testing.md §1b）：對同一 teamSlotId 灌 N 個併發「補位」，
// 量 TeamSlotService.UpdateAsync 取 classId 1002 advisory lock 的正常排隊等待時間，
// 跟 RegistrationLock 的 lock_timeout 預設 5s 比較（會不會誤把「排隊久」當「持鎖方卡死」）。
//
// 前置：db/seed-load-teamslot.sql 已跑過（N 個空位在同一支隊，discordId 9100001..9100000+N，
// characterId edit1..editN；TRUNCATE...RESTART IDENTITY 之後 periodId/bossId/teamSlotId 固定是 1）。
//
// 跑法：
//   docker run --rm -v "$(pwd)/k6:/scripts" -e BASE_URL=http://host.docker.internal:5230 \
//     -e VUS=60 grafana/k6 run /scripts/teamslot-edit-load.js
//
// 每個 VU 填「不同」空位（by index），刻意避開樂觀鎖版本衝突——衝突率高不代表悲觀鎖排隊久，
// 這裡只想單獨量「拿 classId 1002 鎖」本身的等待分布。

import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://host.docker.internal:5230';
const VUS = parseInt(__ENV.VUS || '60', 10);
const BOSS_ID = parseInt(__ENV.BOSS_ID || '1', 10);
const TEAM_SLOT_ID = parseInt(__ENV.TEAM_SLOT_ID || '1', 10);

export const options = {
  scenarios: {
    teamslot_edit_contention: {
      executor: 'per-vu-iterations',
      vus: VUS,
      iterations: 1,
      maxDuration: '2m',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.05'],
  },
};

function uuidv4() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

export default function () {
  const idx = __VU; // per-vu-iterations：__VU 是 1..VUS
  const discordId = 9100000 + idx;
  const characterId = 'edit' + idx;

  // 1) test-login
  const loginRes = http.post(
    `${BASE_URL}/api/test/login`,
    JSON.stringify({ discordId, discordName: `Edit${idx}`, role: 'user' }),
    { headers: { 'Content-Type': 'application/json', 'X-Idempotency-Key': uuidv4() } }
  );
  const loginOk = check(loginRes, { 'login 200': (r) => r.status === 200 });
  if (!loginOk) return;
  const jwt = loginRes.cookies.jwtToken && loginRes.cookies.jwtToken[0] && loginRes.cookies.jwtToken[0].value;
  if (!jwt) return;

  // 2) 讀當前隊伍狀態——跟真實前端一樣，補位前先讀一次拿到空位的 id + version（xmin）
  const getRes = http.get(`${BASE_URL}/api/teamslot?bossId=${BOSS_ID}`, {
    headers: { Cookie: `jwtToken=${jwt}` },
  });
  const getOk = check(getRes, { 'get 200': (r) => r.status === 200 });
  if (!getOk) return;

  const teamSlots = getRes.json();
  const team = teamSlots.find((t) => t.id === TEAM_SLOT_ID);
  if (!team) return;
  // 🔴 坑：一開始用「目前還是空位的第 idx 個」去選目標——但這是個會隨併發填位動態縮短的
  // list，不同 VU 在不同時間點呼叫 GET，各自看到的「剩餘空位」排序/數量都不一樣，會導致
  // 兩個 VU 算出同一個實際 row（誤觸樂觀鎖衝突，量到的是假衝突，不是真排隊延遲）。
  // 改成直接認 DB row id（seed 腳本用 FOR 迴圈依序插入，TRUNCATE...RESTART IDENTITY 後
  // 保證 id 就是 1..N）——每個 VU 認定的目標固定不變，不受其他 VU 進度影響。
  const target = team.characters.find((c) => c.id === idx);
  if (!target) return;

  // 3) 真正的補位請求——N 個 VU 同時打同一個 teamSlotId，全部搶 classId 1002 這把鎖
  const body = JSON.stringify({
    bossId: BOSS_ID,
    deleteTeamSlotIds: [],
    teamSlots: [
      {
        id: TEAM_SLOT_ID,
        deleteTeamSlotCharacterIds: [],
        characters: [
          {
            id: target.id,
            discordId: String(discordId),
            characterId,
            version: target.version,
          },
        ],
      },
    ],
  });
  const putRes = http.put(`${BASE_URL}/api/teamslot`, body, {
    headers: {
      'Content-Type': 'application/json',
      'X-Idempotency-Key': uuidv4(),
      Cookie: `jwtToken=${jwt}`,
    },
    tags: { name: 'put_teamslot' }, // 獨立標記，跟 login/get 的延遲分開看——這段才是真正卡 classId 1002 鎖的請求
  });
  check(putRes, {
    'put 200': (r) => r.status === 200,
    'no conflict': (r) => {
      try {
        return JSON.parse(r.body).conflictedTeamSlotIds.length === 0;
      } catch {
        return false;
      }
    },
  });
}
