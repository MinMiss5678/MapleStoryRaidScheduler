import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// leader-led 隊長轉讓（需同意）：隊長開隊 → 玩家申請入隊 → 隊長轉讓隊長給該成員 → 成員接受 → 成員成為新隊長。
// 覆蓋：/teams/new、/me/led-teams（轉讓控制 + roster）、/teams/[id]/applications、/me/teams（轉讓收件匣）。
// 隔離：隊長 7001（test-login 自建）+ 目標 P-Trans(7002,c7002,seed)。唯一 slot+desc 保冪等。
test('隊長開隊 → 玩家入隊 → 轉讓隊長 → 對方接受成為新隊長', async ({ page }) => {
  test.slow();
  page.on('dialog', d => d.accept());

  const salt = Date.now();
  const pad = (n: number) => String(n).padStart(2, '0');
  const slot = new Date();
  slot.setDate(slot.getDate() + 10 + (salt % 7));
  const slotLocal = `${slot.getFullYear()}-${pad(slot.getMonth() + 1)}-${pad(slot.getDate())}T${pad(salt % 24)}:${pad((salt % 2) * 30)}`;   // step=1800 → 分鐘只能 00/30
  const desc = `LL-transfer-${salt}`;

  // 1) 隊長 7001 開隊
  await loginAs(page, { discordId: 7001, name: 'P-Trans-Leader', role: 'user' });
  await page.goto('/teams/new');
  await page.locator('select').selectOption({ label: 'E2E王' });
  await page.locator('input[type="datetime-local"]').fill(slotLocal);
  await page.locator('textarea').fill(desc);
  await page.getByRole('button', { name: '開隊', exact: true }).click();
  await page.waitForURL(u => new URL(u).pathname === '/me/led-teams');

  // 2) 玩家 7002 申請
  await loginAs(page, { discordId: 7002, name: 'P-Trans', role: 'user' });
  await page.goto('/teams/open');
  const openCard = page.locator('li').filter({ hasText: desc });
  await expect(openCard).toBeVisible();
  await openCard.locator('select').selectOption('c7002');
  await Promise.all([
    page.waitForResponse(r => r.url().includes('/Applications') && r.request().method() === 'POST' && r.ok()),
    openCard.getByRole('button', { name: '申請' }).click(),
  ]);

  // 3) 隊長核准 → 7002 入隊
  await loginAs(page, { discordId: 7001, name: 'P-Trans-Leader', role: 'user' });
  await page.goto('/me/led-teams');
  const hubCard = page.locator('li').filter({ hasText: desc });
  await hubCard.getByRole('link', { name: /審核申請/ }).click();
  await page.waitForURL(/\/teams\/\d+\/applications/);
  await page.getByRole('button', { name: '核准' }).click();
  await expect(page.getByText('目前沒有待審核的申請')).toBeVisible();

  // 4) 隊長轉讓隊長給該成員
  await page.goto('/me/led-teams');
  const hubCard2 = page.locator('li').filter({ hasText: desc });
  await hubCard2.getByRole('button', { name: '轉讓隊長' }).click();
  await hubCard2.locator('select').selectOption({ index: 1 }); // 唯一 Confirmed 成員 C-Trans
  await Promise.all([
    page.waitForResponse(r => /\/teamSlot\/\d+\/TransferLeader$/.test(new URL(r.url()).pathname) && r.request().method() === 'POST' && r.ok()),
    hubCard2.getByRole('button', { name: '送出' }).click(),
  ]);

  // 5) 7002 接受轉讓 → 成為新隊長
  await loginAs(page, { discordId: 7002, name: 'P-Trans', role: 'user' });
  await page.goto('/me/teams');
  await expect(page.getByText(/隊長轉讓/)).toBeVisible();
  await Promise.all([
    page.waitForResponse(r => /\/teamSlot\/\d+\/TransferLeader$/.test(new URL(r.url()).pathname) && r.request().method() === 'PUT' && r.ok()),
    page.getByRole('button', { name: '接受當隊長' }).click(),
  ]);

  // 6) 7002 的帶隊 hub 出現此隊（現在是新隊長）
  await page.goto('/me/led-teams');
  await expect(page.locator('li').filter({ hasText: desc })).toBeVisible();
});
