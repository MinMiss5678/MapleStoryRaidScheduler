import { test, expect, type APIRequestContext } from '@playwright/test';
import { loginAs } from './helpers/auth';
import {
  BACKEND_URL,
  backendRequest,
  forgedSessionCookie,
  idempotencyHeader,
  loginDirect,
  roleTamperedJwtCookie,
  wrongKeyResignedJwtCookie,
  type Identity,
} from './helpers/forge';

/**
 * 授權矩陣護欄（G1 / G2 / G8 / G10）——case 表見 plans/2026-11-04-authorization-matrix.md。
 *
 * 目的：在改認證主幹（階段 0 拆授權、階段 3 改續期）**之前**先立起端到端斷言。
 * 這個系統的授權層出過三次問題，其中同樣三行出錯兩次（0ed4125 → 被 f0f32fb 重構帶回 → 60faf7b）。
 *
 * G1/G2/G8 **直打後端**（繞過 Next proxy，見 helpers/forge.ts 的理由）；
 * G10 反過來**刻意經過 proxy**，專測白名單沒擋掉該過的路徑。
 */

/**
 * 專屬 discordId 區段，避免與其他 spec 的身分互相干擾（fullyParallel）。
 *
 * ⚠️ **必須每個 worker 用不同的 discordId**，否則 admin 的 session 測試會偽陽性失敗（401）。
 *
 * 原因是 `SessionService` 的設計：Redis 快取**以 discordId 為 key**，所以同一個 Discord 使用者
 * 在快取有效期內**只能有一個活躍 session**。兩個 worker 都用同一個 discordId 登入時：
 *   worker 1 登入 → sessionId=S1；worker 2 登入 → sessionId=S2（DB 兩筆都在）
 *   worker 1 發請求 → 快取未命中 → 查 DB → 寫入 快取[discordId] = session(S1)
 *   worker 2 發請求 → 快取**命中** → `cached.SessionId(S1) != sessionId(S2)` → **401**
 *
 * 也就是 `c0e0311` 那道防護正確地擋掉了 worker 2 的合法 session。
 * 這是測試骨架要避開的衝突；它同時暴露一個產品行為：登入流程（`AuthAppService`）不會清
 * `cache[discordId]`，所以管理員在舊 session 仍在快取時**重新登入**（換瀏覽器、清 cookie），
 * 新 session 會被擋成 401，最長 `SessionPolicy.CacheFreshness`（15 分鐘）。
 *
 * ⚠️ 還有第二個坑：**本機重複執行**時固定 id 一樣會撞。
 * `db/seed-e2e.sql` 的 TRUNCATE 清單是 `TeamSlotCharacter / TeamSlot / Character / Player / Boss`
 * ——**沒有 `Session`**，而 Redis 也不會被 seed 清掉。
 * 所以同一個 discordId 的舊 session 會跨執行累積，且 `cache[discordId]` 可能還卡著舊的那筆
 * → 新登入的 sessionId 與快取裡的不符 → 401（即使只有一個 worker）。
 *
 * → id 要**每次執行 ＋ 每個 worker 都唯一**。CI 每次都是全新資料庫，不受影響；
 *   本機則靠這個 salt 讓每次執行都拿到乾淨的身分。
 */
// 週期約 11.6 天，遠大於快取有效期 15 分鐘 → 不同執行之間不會撞到仍有效的舊快取
const RUN_SALT = Date.now() % 1_000_000_000;
const idFor = (slot: number) => 970_000_000 + RUN_SALT * 100 + test.info().workerIndex * 10 + slot;

let USER_A: number;
let ADMIN: number;

type Endpoint = { method: 'GET' | 'POST' | 'PUT' | 'DELETE'; path: string; label: string };

/** 代表性的受保護端點（非 AllowAnonymous）。寫入方法一律帶 idempotency key。 */
const PROTECTED: Endpoint[] = [
  { method: 'GET', path: '/api/Character/GetWithDiscordName', label: 'Character 讀' },
  { method: 'POST', path: '/api/Character', label: 'Character 寫' },
  { method: 'GET', path: '/api/Profile', label: 'Profile 讀' },
  { method: 'PUT', path: '/api/Profile', label: 'Profile 寫' },
  { method: 'GET', path: '/api/TeamSlot/Open', label: 'TeamSlot 讀' },
  { method: 'POST', path: '/api/TeamSlot', label: 'TeamSlot 寫' },
  { method: 'GET', path: '/api/Me/Invitations', label: 'Me/Invitations' },
  { method: 'GET', path: '/api/Me/Teams', label: 'Me/Teams' },
  { method: 'GET', path: '/api/Me/LedTeams', label: 'Me/LedTeams' },
  { method: 'GET', path: '/api/Me/LeaderTransfers', label: 'Me/LeaderTransfers' },
  { method: 'GET', path: '/api/LfgIntent', label: 'LfgIntent 讀' },
  { method: 'POST', path: '/api/LfgIntent', label: 'LfgIntent 寫' },
  { method: 'GET', path: '/api/AvailabilityOverride', label: 'AvailabilityOverride 讀' },
  { method: 'POST', path: '/api/AvailabilityOverride', label: 'AvailabilityOverride 寫' },
  { method: 'GET', path: '/api/Boss/GetAll', label: 'Boss 讀' },
  { method: 'GET', path: '/api/SystemConfig', label: 'SystemConfig 讀' },
];

/** 唯四個有宣告式 [AuthorizeRole("admin")] 的端點。 */
const ADMIN_ONLY: Endpoint[] = [
  { method: 'POST', path: '/api/Boss', label: 'POST /api/Boss' },
  { method: 'PUT', path: '/api/Boss/999999', label: 'PUT /api/Boss/{id}' },
  { method: 'DELETE', path: '/api/Boss/999999', label: 'DELETE /api/Boss/{id}' },
  { method: 'POST', path: '/api/SystemConfig', label: 'POST /api/SystemConfig' },
];

const isWrite = (m: Endpoint['method']) => m !== 'GET';

async function call(ctx: APIRequestContext, ep: Endpoint, cookie?: string) {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  if (cookie) headers.Cookie = cookie;
  if (isWrite(ep.method)) Object.assign(headers, idempotencyHeader());

  const options = { headers, ...(isWrite(ep.method) ? { data: {} } : {}) };
  switch (ep.method) {
    case 'GET':
      return ctx.get(ep.path, options);
    case 'POST':
      return ctx.post(ep.path, options);
    case 'PUT':
      return ctx.put(ep.path, options);
    case 'DELETE':
      return ctx.delete(ep.path, options);
  }
}

let api: APIRequestContext;
let userA: Identity;
let admin: Identity;

test.beforeAll(async () => {
  USER_A = idFor(0);
  ADMIN = idFor(1);
  // api 的 cookie jar 永遠是空的（登入用的是 loginDirect 內部用完即丟的 context），
  // 身分一律以明確的 Cookie header 帶入 → 沒帶 header 的請求就是真的匿名。
  api = await backendRequest();
  userA = await loginDirect({ discordId: USER_A, role: 'user' });
  admin = await loginDirect({ discordId: ADMIN, role: 'admin' });
});

test.afterAll(async () => {
  await api.dispose();
});

// ---------------------------------------------------------------- G1 匿名邊界

test.describe('G1 匿名邊界', () => {
  for (const ep of PROTECTED) {
    test(`G1.1 匿名打 ${ep.label}（${ep.method} ${ep.path}）→ 401`, async () => {
      const res = await call(api, ep);
      expect(res.status(), `${ep.method} ${ep.path}`).toBe(401);
    });
  }

  // 未登入打 admin-only 端點也必須是 401（不是 403）——守 0ed4125 的「401/403 語意修正」：
  // 連你是誰都不知道，就不是「權限不足」。
  for (const ep of ADMIN_ONLY) {
    test(`G1.1 匿名打 admin-only ${ep.label} → 401（不得 403）`, async () => {
      const res = await call(api, ep);
      expect(res.status(), ep.label).toBe(401);
    });
  }

  test('G1.2 / G1.3 Auth 的 login / logout 為 AllowAnonymous → 非 401', async () => {
    for (const path of ['/api/Auth/login', '/api/Auth/logout']) {
      const res = await api.post(path, {
        headers: { 'Content-Type': 'application/json', ...idempotencyHeader() },
        data: {},
      });
      expect(res.status(), path).not.toBe(401);
    }
  });

  test('G1.4 test-login 在非 Production 可用 → 200', async () => {
    const res = await api.post('/api/test/login', {
      headers: { 'Content-Type': 'application/json', ...idempotencyHeader() },
      data: { discordId: idFor(3), discordName: 'E2E-matrix', role: 'user' },
    });
    expect(res.status()).toBe(200);
  });

  test('G1.5 alert-mail 不帶 X-Alert-Secret → 401', async () => {
    // /api/internal 被 IdempotencyMiddleware 排除 → 不需帶 idempotency key。
    // AllowAnonymous 只是繞過認證 middleware，controller 自己驗共用密鑰（且 fail-closed）。
    const res = await api.post('/api/internal/alert-mail', {
      headers: { 'Content-Type': 'application/json' },
      data: { subject: 'e2e', body: 'e2e' },
    });
    expect(res.status()).toBe(401);
  });

  test('G1.6 alert-mail 帶錯誤的 X-Alert-Secret → 401', async () => {
    const res = await api.post('/api/internal/alert-mail', {
      headers: { 'Content-Type': 'application/json', 'X-Alert-Secret': 'wrong-secret' },
      data: { subject: 'e2e', body: 'e2e' },
    });
    expect(res.status()).toBe(401);
  });
});

// ---------------------------------------------------- G2 admin-only 端點

test.describe('G2 admin-only 端點', () => {
  for (const ep of ADMIN_ONLY) {
    test(`G2 一般使用者打 ${ep.label} → 403`, async () => {
      const res = await call(api, ep, userA.cookie);
      expect(res.status(), ep.label).toBe(403);
    });
  }

  /**
   * G2.5 只驗「授權放行」，**不得改動共用狀態**（測試是平行跑的）：
   * - Boss 的 POST / PUT 送空 body → `BossRequest.Name` 是 [Required] → 模型驗證回 400，不會寫入
   * - Boss 的 DELETE 打不存在的 id 999999 → 無資料可刪
   * - SystemConfig 的 POST **沒有任何 [Required]**，送空 body 會綁成全預設值並**覆寫系統設定**
   *   → 改成先讀出目前設定、原封不動寫回（no-op 寫入，但仍走過授權）
   */
  for (const ep of ADMIN_ONLY.filter((e) => e.path !== '/api/SystemConfig')) {
    test(`G2.5 管理員打 ${ep.label} → 非 401／403（授權放行）`, async () => {
      const res = await call(api, ep, admin.cookie);
      expect([401, 403], `${ep.label} 回了 ${res.status()}`).not.toContain(res.status());
    });
  }

  test('G2.5 管理員打 POST /api/SystemConfig（原值寫回）→ 非 401／403（授權放行）', async () => {
    const current = await api.get('/api/SystemConfig', { headers: { Cookie: admin.cookie } });
    expect(current.status(), '讀取目前設定').toBe(200);

    const res = await api.post('/api/SystemConfig', {
      headers: { 'Content-Type': 'application/json', Cookie: admin.cookie, ...idempotencyHeader() },
      data: await current.json(),
    });
    expect([401, 403], `POST /api/SystemConfig 回了 ${res.status()}`).not.toContain(res.status());
  });

  test('G2.6 一般使用者讀 Boss / SystemConfig → 非 403（讀取不限管理員）', async () => {
    for (const path of ['/api/Boss/GetAll', '/api/SystemConfig']) {
      const res = await api.get(path, { headers: { Cookie: userA.cookie } });
      expect(res.status(), path).not.toBe(403);
    }
  });
});

// -------------------------------------------- G8 認證繞過（已修漏洞的回歸）

test.describe('G8 認證繞過回歸', () => {
  /**
   * ⚠️ 必須先用**真的** admin cookie 打一次，才測得到漏洞所在的路徑。
   *
   * `c0e0311` 的漏洞在 `SessionService` 的**快取命中**分支：Redis 以 **discordId** 為 key，
   * 命中時若沒比對傳入的 sessionId 就直接放行。
   * 而快取不是登入時寫入的（`CreateAsync` 只寫資料庫），是**第一次成功驗證**時才由 `CacheAsync` 寫入。
   *
   * 若不預熱，偽造請求會走「快取未命中 → 查資料庫 → 查無 → 401」，
   * 雖然同樣回 401，但**完全沒碰到快取命中的比對**——拿掉 `cached.SessionId != sessionId`
   * 這行防護，測試照樣綠。那就是 covered ≠ tested。
   */
  async function warmSessionCache() {
    const warm = await api.get('/api/Me/Teams', { headers: { Cookie: admin.cookie } });
    expect(warm.status(), '預熱請求必須成功，否則下面測不到 cache-hit 比對').toBe(200);
  }

  test('G8.1 偽造 sessionId ＋ 真 discordId → 401（守 c0e0311 session 冒用）', async () => {
    await warmSessionCache();
    const res = await api.get('/api/Me/Teams', { headers: { Cookie: forgedSessionCookie(ADMIN) } });
    expect(res.status()).toBe(401);
  });

  test('G8.2 偽造 session 打 admin-only 端點 → 401，絕不可 200', async () => {
    await warmSessionCache();
    const res = await call(api, ADMIN_ONLY[0], forgedSessionCookie(ADMIN));
    expect(res.status()).toBe(401);
  });

  test('G8.3 簽章錯誤的 JWT（payload 全合法、只有簽章錯）→ 401', async () => {
    const realJwt = userA.cookies['jwtToken'];
    expect(realJwt, 'test-login(role=user) 應發出 jwtToken cookie').toBeTruthy();

    // 先確認真 token 本身可用，才能把下面的 401 歸因到「簽章」
    const ok = await api.get('/api/Me/Teams', { headers: { Cookie: userA.cookie } });
    expect(ok.status(), '原始 JWT 應能通過認證').toBe(200);

    const res = await api.get('/api/Me/Teams', { headers: { Cookie: wrongKeyResignedJwtCookie(realJwt) } });
    expect(res.status()).toBe(401);
  });

  test('G8.4 把 role 竄改成 admin（簽章失效）打 admin-only → 401 或 403，絕不可 200', async () => {
    const realJwt = userA.cookies['jwtToken'];
    expect(realJwt, 'test-login(role=user) 應發出 jwtToken cookie').toBeTruthy();

    const res = await call(api, ADMIN_ONLY[0], roleTamperedJwtCookie(realJwt));
    expect([401, 403], `回了 ${res.status()}`).toContain(res.status());
  });

  test('G8.5 一般使用者（簽章正確）打 admin-only → 403（守 0ed4125 比對 role claim）', async () => {
    const res = await call(api, ADMIN_ONLY[0], userA.cookie);
    expect(res.status()).toBe(403);
  });

  // G8.6 / G8.7（過期但簽章有效的 JWT → 續期後仍受角色檢查，守 60faf7b）需要
  // test-login 支援 ttlSeconds＝改實作 → 留到獨立循環。單元層已有
  // AuthenticationMiddlewareTests.ExpiredJwt_RefreshesAndEnforcesNewRole 覆蓋。
});

// ------------------------------------------ G10 proxy 白名單（刻意經過 proxy）

test.describe('G10 proxy 白名單回歸', () => {
  // 與上面相反：這組**必須**經過 Next proxy，驗證白名單沒擋掉合法路徑。
  // 歷史 bug：白名單漏了 'me' → /api/Me/* 全部 403。
  const PROXIED = [
    '/api/Me/Invitations',
    '/api/Me/Teams',
    '/api/Me/LedTeams',
    '/api/Me/LeaderTransfers',
    '/api/Character/GetWithDiscordName',
    '/api/Profile',
  ];

  test('G10 一般使用者經前端 proxy 讀自助 API → 非 403', async ({ page }) => {
    // 另用一組 id：這是瀏覽器 context，與上面直打後端的身分分開，避免 cookie 互擾
    await loginAs(page, { discordId: idFor(2), name: 'E2E-matrix-proxy', role: 'user' });

    for (const path of PROXIED) {
      const res = await page.request.get(path);
      expect(res.status(), `proxy ${path}（403 代表白名單擋掉了）`).not.toBe(403);
    }
  });
});

test('矩陣是直打後端，不是打前端', () => {
  // 防呆：若有人把 BACKEND_URL 指到 3000，整個矩陣會變成在測 proxy，403 的來源就不可信。
  expect(BACKEND_URL, 'E2E_BACKEND_URL 不應指向前端 (3000)').not.toContain(':3000');
});
