import { createHmac, randomUUID } from 'crypto';
import { request, type APIRequestContext, type APIResponse } from '@playwright/test';

/**
 * 授權矩陣專用 helper：**直打後端**，不經 Next proxy。
 *
 * 為什麼要繞過 proxy：proxy 有路徑白名單（`constants/apiWhitelist.ts`），擋下的路徑回 **403**。
 * 若矩陣經過 proxy，一個 403 可能來自路由而非授權 → 測試會「假通過」，完全失去意義。
 * （proxy 白名單本身另由 `authorization-matrix.spec.ts` 的 G10 專測。）
 *
 * 為什麼自己組 `Cookie` header 而不靠 cookie jar：後端發的 cookie 都帶 `Secure`，
 * 而 CI 裡 playwright 容器是用 `http://e2e-backend:5230`（非 localhost → 不是 secure context），
 * 瀏覽器規則會丟掉 Secure cookie。直接組 header 可完全避開這個行為差異，
 * 而且偽造身分（G8）本來就必須自訂 cookie 值。
 */

/** 本機＝published port；CI＝compose 網路內的服務名（見 compose.e2e.yaml 的 E2E_BACKEND_URL）。 */
export const BACKEND_URL = process.env.E2E_BACKEND_URL || 'http://localhost:5230';

/** 直打後端的 request context。呼叫端負責 dispose。 */
export function backendRequest(): Promise<APIRequestContext> {
  return request.newContext({ baseURL: BACKEND_URL });
}

/** POST/PUT/DELETE 必帶：IdempotencyMiddleware 跑在認證**之前**，沒帶會先被擋成 400 而不是 401。 */
export function idempotencyHeader(): Record<string, string> {
  return { 'X-Idempotency-Key': randomUUID() };
}

/** 從回應的多個 Set-Cookie 取出 name=value（忽略 Path/Secure/HttpOnly 等屬性）。 */
function parseSetCookies(res: APIResponse): Record<string, string> {
  const out: Record<string, string> = {};
  for (const h of res.headersArray()) {
    if (h.name.toLowerCase() !== 'set-cookie') continue;
    const [pair] = h.value.split(';');
    const eq = pair.indexOf('=');
    if (eq > 0) out[pair.slice(0, eq).trim()] = pair.slice(eq + 1).trim();
  }
  return out;
}

const toCookieHeader = (cookies: Record<string, string>) =>
  Object.entries(cookies)
    .map(([k, v]) => `${k}=${v}`)
    .join('; ');

export type Identity = { cookie: string; cookies: Record<string, string> };

/**
 * 以 test-login 取得**真實**身分的 cookie（直接打後端，不經 proxy）。
 * role='user' → `jwtToken`；role='admin' → `sessionId{discordId}` + `discordId`。
 *
 * ⚠️ 用**用完即丟**的 context 登入，不共用呼叫端的 context：
 * test-login 的 Set-Cookie 可能被存進 context 的 cookie jar（本機 `localhost` 是 secure context，
 * Secure cookie 會被接受）。若共用，之後「匿名」請求會被 jar 自動帶上身分 → G1 的 401 斷言失真。
 */
export async function loginDirect(opts: {
  discordId: number;
  name?: string;
  role?: 'user' | 'admin';
}): Promise<Identity> {
  const { discordId, name = `E2E-${discordId}`, role = 'user' } = opts;

  const ctx = await backendRequest();
  try {
    const res = await ctx.post('/api/test/login', {
      headers: { 'Content-Type': 'application/json', ...idempotencyHeader() },
      data: { discordId, discordName: name, role },
    });
    if (!res.ok()) throw new Error(`test-login 失敗 ${res.status()}：${await res.text()}`);

    const cookies = parseSetCookies(res);
    if (Object.keys(cookies).length === 0) throw new Error('test-login 沒有回傳任何 Set-Cookie');

    return { cookie: toCookieHeader(cookies), cookies };
  } finally {
    await ctx.dispose();
  }
}

// ---------------- 偽造身分（G8：認證繞過回歸） ----------------

/**
 * `SESSION_FORGED`：帶**真的** discordId ＋ **亂數** sessionId。
 *
 * 守 `c0e0311`：`SessionService` 的 Redis 快取是以 **discordId** 為 key，
 * 快取命中時若沒比對傳入的 sessionId，任何人帶管理員的 discordId（Discord ID 是公開資訊）
 * ＋ 任意 sessionId 就能冒用管理員。
 */
export function forgedSessionCookie(discordId: number): string {
  return toCookieHeader({
    discordId: String(discordId),
    [`sessionId${discordId}`]: randomUUID().replace(/-/g, ''),
  });
}

const b64url = (input: string) => Buffer.from(input).toString('base64url');

/**
 * `JWT_FORGED_SIG`：拿**真的** JWT，header 與 payload **原封不動**，只用**錯誤金鑰**重簽。
 *
 * 為什麼不自己捏 payload：若 issuer / audience 也是假的，401 可能來自 issuer 驗證而非簽章驗證，
 * 那這條就測不到「驗簽」本身。沿用真 payload（issuer / audience / exp 都合法）→
 * **唯一的錯誤就是簽章**，401 才能歸因到驗簽。
 */
export function wrongKeyResignedJwtCookie(realJwt: string): string {
  const [header, payload, signature] = realJwt.split('.');
  if (!header || !payload || !signature) throw new Error(`不是合法的 JWT 結構：${realJwt}`);

  const forged = createHmac('sha256', 'this-is-definitely-not-the-real-signing-key')
    .update(`${header}.${payload}`)
    .digest('base64url');

  return toCookieHeader({ jwtToken: `${header}.${payload}.${forged}` });
}

/**
 * `JWT_ROLE_TAMPERED`：拿**真的** JWT，把 role claim 從 `user` 改成 `admin`，**保留原簽章**。
 * → payload 變了但簽章沒重算 → 驗簽必敗。
 *
 * 守 `0ed4125`（授權改為比對 role claim）：role 寫在 token 裡之所以安全，
 * 全靠「改一個 byte 就驗簽失敗」。這條就是在證明那個前提。
 *
 * 不寫死 claim 名稱（`ClaimTypes.Role` 的輸出名稱取決於 claim type 對應設定），
 * 改成**找出值等於原 role 的欄位**再改，避免依賴實作細節。
 */
export function roleTamperedJwtCookie(realJwt: string, fromRole = 'user', toRole = 'admin'): string {
  const [header, payload, signature] = realJwt.split('.');
  if (!header || !payload || !signature) throw new Error(`不是合法的 JWT 結構：${realJwt}`);

  const claims = JSON.parse(Buffer.from(payload, 'base64url').toString('utf8')) as Record<string, unknown>;
  let tampered = false;
  for (const [k, v] of Object.entries(claims)) {
    if (v === fromRole) {
      claims[k] = toRole;
      tampered = true;
    }
  }
  if (!tampered) throw new Error(`JWT 裡找不到值為 "${fromRole}" 的 claim，無法竄改：${JSON.stringify(claims)}`);

  return toCookieHeader({ jwtToken: `${header}.${b64url(JSON.stringify(claims))}.${signature}` });
}
