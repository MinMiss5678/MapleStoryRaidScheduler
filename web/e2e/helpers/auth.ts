import { randomUUID } from 'crypto';
import type { Page } from '@playwright/test';

export type TestRole = 'user' | 'admin';

/**
 * 以捏造身分登入（繞過 Discord OAuth）：呼叫後端 test-login 端點（經 Next `/api` proxy），
 * 拿到與正式流程一樣的 cookie 並種進 browser context。之後 `page.goto` 即為登入態。
 * 需要：後端在**非 Production** 環境（test-login 才有效）+ DB 可寫。
 */
export async function loginAs(
  page: Page,
  opts: { discordId?: number; name?: string; role?: TestRole } = {},
): Promise<void> {
  const { discordId = 999001, name = 'E2E玩家', role = 'user' } = opts;

  const res = await page.request.post('/api/test/login', {
    headers: {
      'Content-Type': 'application/json',
      'X-Idempotency-Key': randomUUID(), // IdempotencyMiddleware 要求 POST 帶 UUID
    },
    data: { discordId, discordName: name, role },
  });

  if (!res.ok()) {
    throw new Error(`test-login 失敗 ${res.status()}：${await res.text()}`);
  }
}
