// Phase 2（plans/2026-07-28-load-testing.md §1b；period-less 重指版）：
// 對同一 teamSlotId 灌 N 個併發「接受邀請」，量 TeamLeaderService.ConfirmMemberAsync 取
// classId 1002 advisory lock（AcquireTeamSlotEditLockAsync）的正常排隊等待時間，跟 lock_timeout
// 預設 5s 比較（會不會誤把「排隊久」當「持鎖方卡死」）。
//
// （原本打 PUT /api/teamslot 補位——該端點與 TeamSlotService 已於 period-less 4c-be 退場；
//   同一把 classId 1002 鎖搬到了 ConfirmMemberAsync，這裡重指到現行熱路徑。）
//
// 前置：db/seed-load-confirm.sql 已跑過（teamSlotId=1、容量=N、N 筆 Invited 成員，
// memberId=1..N，玩家 discordId=9100001..9100000+N）。容量=N 故全部 accept 都成功——
// 純量「拿 classId 1002 鎖」本身的排隊延遲，不摻「隊伍已滿」拒絕的雜訊。
//
// 跑法：
//   docker run --rm -v "$(pwd)/k6:/scripts" -e BASE_URL=http://host.docker.internal:5230 \
//     -e VUS=60 grafana/k6 run /scripts/confirm-accept-load.js

import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://host.docker.internal:5230';
const VUS = parseInt(__ENV.VUS || '60', 10);
const TEAM_SLOT_ID = parseInt(__ENV.TEAM_SLOT_ID || '1', 10);

export const options = {
  scenarios: {
    confirm_contention: {
      executor: 'per-vu-iterations',
      vus: VUS,
      iterations: 1,
      maxDuration: '2m',
    },
  },
  thresholds: {
    // 真正的 lock_timeout 訊號：accept 逾時會拋 AdvisoryLockTimeoutException → BusinessException
    // 「隊伍忙碌中」→ 非 2xx。故 http_req_failed=0% 就代表 lock_timeout 從未誤觸發（成員全數 Confirmed）。
    http_req_failed: ['rate<0.05'],
    // accept 子指標只當「client 觀察延遲」的參考印出（含進交易前的 Kestrel/連線池排隊，非 lock 本身等待）；
    // 給寬鬆上界避免把「client 延遲高」誤當「lock 卡死」——後者只看 http_req_failed。
    'http_req_duration{name:accept_invite}': ['p(99)<30000'],
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
  const memberId = idx; // seed 依序插入、RESTART IDENTITY → TeamSlotCharacter.Id = 1..N，對齊 __VU

  // 1) test-login 成「自己」（accept 只能接受 member.DiscordId == 登入身分 的邀請）
  const loginRes = http.post(
    `${BASE_URL}/api/test/login`,
    JSON.stringify({ discordId, discordName: `Cand${idx}`, role: 'user' }),
    { headers: { 'Content-Type': 'application/json', 'X-Idempotency-Key': uuidv4() } }
  );
  if (!check(loginRes, { 'login 200': (r) => r.status === 200 })) return;
  const jwt = loginRes.cookies.jwtToken && loginRes.cookies.jwtToken[0] && loginRes.cookies.jwtToken[0].value;
  if (!jwt) return;

  // 2) 併發接受自己的邀請——N 個 VU 同時打同一個 teamSlotId，全部搶 classId 1002 這把鎖。
  //    ConfirmMemberAsync 在鎖內重讀 Confirmed 數 vs 容量 + xmin 改狀態；容量=N 故都成功。
  const acceptRes = http.put(
    `${BASE_URL}/api/teamSlot/${TEAM_SLOT_ID}/Invitations/${memberId}`,
    JSON.stringify({ action: 'accept' }),
    {
      headers: {
        'Content-Type': 'application/json',
        'X-Idempotency-Key': uuidv4(),
        Cookie: `jwtToken=${jwt}`,
      },
      tags: { name: 'accept_invite' }, // 獨立標記——這段才是真正卡 classId 1002 鎖的請求，跟 login 延遲分開看
    }
  );
  check(acceptRes, { 'accept 200': (r) => r.status === 200 });
}
