// Phase 1（plans/2026-07-28-load-testing.md §1）：對同一 period 灌 N 個併發報名，
// 真正對 TeamSlotAutoAssignService 的 classId 1001 advisory lock 製造併發。
//
// 前置：db/seed-load.sql 已跑過（N 個未報名玩家，discordId 9000001..9000000+N，
// characterId load1..loadN；TRUNCATE...RESTART IDENTITY 之後 periodId/bossId 固定是 1）。
//
// 跑法：
//   docker run --rm -v "$(pwd)/k6:/scripts" -e BASE_URL=http://host.docker.internal:5230 \
//     -e VUS=60 grafana/k6 run /scripts/register-load.js
//
// 驗收（跑完後另外查 DB，見 db/verify-load.sql）：
//   - k6 summary 的 error rate（排除刻意的 409/idempotency 之類）
//   - DB 沒有重複隊：同一批玩家的角色分裝進的隊伍數 = ceil(N / RequireMembers)

import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://host.docker.internal:5230';
const VUS = parseInt(__ENV.VUS || '60', 10);
const PERIOD_ID = parseInt(__ENV.PERIOD_ID || '1', 10);
const BOSS_ID = parseInt(__ENV.BOSS_ID || '1', 10);
// 選用：跳過前 OFFSET 個玩家（例如已經在前一輪跑過、報名過的），不用重新 seed 就能連續測試多輪。
const OFFSET = parseInt(__ENV.OFFSET || '0', 10);

export const options = {
  scenarios: {
    same_period_contention: {
      executor: 'per-vu-iterations',
      vus: VUS,
      iterations: 1,
      maxDuration: '2m',
    },
  },
  thresholds: {
    // 正確性靠事後查 DB；這裡只守「沒有非預期的 5xx／逾時」
    http_req_failed: ['rate<0.05'],
  },
};

function uuidv4() {
  // 自製、不依賴外部 jslib（避免跑測時還要拉網路資源）——夠用來當 idempotency key，不用密碼學等級隨機。
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

export default function () {
  const idx = OFFSET + __VU; // per-vu-iterations：__VU 是 1..VUS，+OFFSET 對應一個已 seed 好的玩家
  const discordId = 9000000 + idx;
  const characterId = 'load' + idx;

  // 1) test-login：非 Production 專用後門，直接鑄該玩家的 jwtToken cookie。
  // IdempotencyMiddleware 對所有 POST 生效（不分業務端點），login 也要帶合法 UUID key。
  //
  // 🔴 坑：AuthController 發的 cookie 帶 Secure 旗標（跟正式流程一致）。k6/curl 不是瀏覽器，
  // 沒有「localhost 視為可信來源」那個瀏覽器專屬例外，內建 cookie jar 會照 RFC 把 Secure cookie
  // 吃下但不在後續 http:// 請求重送——導致 register 全部 401。解法：手動從 Set-Cookie 挖出
  // jwtToken 的值，下一支請求自己組 Cookie header 帶過去，繞開 jar 的 Secure 判斷。
  const loginRes = http.post(
    `${BASE_URL}/api/test/login`,
    JSON.stringify({ discordId, discordName: `Load${idx}`, role: 'user' }),
    { headers: { 'Content-Type': 'application/json', 'X-Idempotency-Key': uuidv4() } }
  );
  const loginOk = check(loginRes, { 'login 200': (r) => r.status === 200 });
  if (!loginOk) return;

  const jwt = loginRes.cookies.jwtToken && loginRes.cookies.jwtToken[0] && loginRes.cookies.jwtToken[0].value;
  if (!jwt) return;

  // 2) 真正的報名請求——N 個 VU 同時打，全部對同一個 period 觸發 TeamSlotAutoAssignService.AutoAssignAsync
  const body = JSON.stringify({
    periodId: PERIOD_ID,
    characterRegisters: [{ characterId, bossId: BOSS_ID, rounds: 1 }],
    availabilities: [{ weekday: 2, startTime: '20:00:00', endTime: '22:00:00' }],
  });
  const res = http.post(`${BASE_URL}/api/register`, body, {
    headers: {
      'Content-Type': 'application/json',
      'X-Idempotency-Key': uuidv4(),
      Cookie: `jwtToken=${jwt}`,
    },
  });
  check(res, { 'register 200': (r) => r.status === 200 });
}
