import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// 偏好王軟訊號 E2E：候選(P-Cand 6003)在 /character 設「E2E王」為偏好 → 隊長為該王開隊 →
// 候選頁該角色出現「偏好此王」badge。覆蓋：/character（偏好王 modal）、/teams/new、/teams/[id]/candidates。
// 前置：compose.e2e backend（非 Production）+ db/seed-e2e.sql（P-Cand 6003 英雄・全週可用・參戰中）。
test('候選設偏好王 → 隊長候選頁見「偏好此王」badge', async ({ page }) => {
  test.slow();
  page.on('dialog', d => d.accept());

  const salt = Date.now();
  const pad = (n: number) => String(n).padStart(2, '0');
  const slot = new Date();
  slot.setDate(slot.getDate() + 10 + (salt % 7));
  const slotLocal = `${slot.getFullYear()}-${pad(slot.getMonth() + 1)}-${pad(slot.getDate())}T${pad(salt % 24)}:${pad((salt % 2) * 30)}`;
  const desc = `LL-pref-${salt}`;

  // ── 1) 候選 P-Cand 設偏好王「E2E王」 ──（只有一隻角色 c6003 → 直接找按鈕；checkbox 以 accessible name 精確選）
  await loginAs(page, { discordId: 6003, name: 'P-Cand', role: 'user' });
  await page.goto('/character');
  await page.getByRole('button', { name: '偏好王' }).click();
  await page.getByRole('checkbox', { name: 'E2E王', exact: true }).check();
  await Promise.all([
    page.waitForResponse(r => /\/PreferredBosses$/.test(new URL(r.url()).pathname) && r.request().method() === 'PUT' && r.ok()),
    page.getByRole('button', { name: '儲存' }).click(),
  ]);

  // ── 2) 隊長開隊（E2E王・英雄） ──
  await loginAs(page, { discordId: 6004, name: 'P-Pref-Leader', role: 'user' });
  await page.goto('/teams/new');
  await page.locator('select').selectOption({ label: 'E2E王' });
  await page.locator('input[type="datetime-local"]').fill(slotLocal);
  await page.locator('textarea').fill(desc);
  await page.locator('label').filter({ hasText: '英雄' }).click();
  await page.getByRole('button', { name: '開隊', exact: true }).click();
  await page.waitForURL(u => new URL(u).pathname === '/me/led-teams');

  // ── 3) 候選頁：P-Cand 帶「偏好此王」badge ──
  const hubCard = page.locator('li').filter({ hasText: desc });
  await hubCard.getByRole('link', { name: /挑候選/ }).click();
  await page.waitForURL(/\/teams\/\d+\/candidates/);
  const candRow = page.locator('li').filter({ hasText: 'P-Cand' });
  await expect(candRow).toBeVisible();
  await expect(candRow).toContainText('偏好此王');
});
