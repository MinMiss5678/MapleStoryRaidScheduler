import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// leader-led 自動撤銷過期邀請（mutation-ux Tier 3）：容量 1 的隊 → 隊長邀 A、B 兩人 → A 接受使隊伍額滿
// → B 的邀請被自動撤銷（從 /me/teams 待處理邀請消失）。先驗 B 有邀請、再驗 A 接受後 B 沒了 → 證明因果非「從未邀」。
// 覆蓋：/teams/new、/teams/[id]/candidates（邀請）、/me/teams（待處理邀請 + 接受）。
// 隔離：容量 1 的王「E2E王滿」+ 候選 P-Full-A(6005)/P-Full-B(6006)（全週可用, seed）+ 隊長 6007（test-login 自建）。
test('容量 1 隊：一人接受額滿 → 另一人邀請自動撤銷', async ({ page }) => {
  test.slow();
  page.on('dialog', d => d.accept());

  const salt = Date.now();
  const pad = (n: number) => String(n).padStart(2, '0');
  const slot = new Date();
  slot.setDate(slot.getDate() + 10 + (salt % 7));
  const slotLocal = `${slot.getFullYear()}-${pad(slot.getMonth() + 1)}-${pad(slot.getDate())}T${pad(salt % 24)}:${pad(salt % 60)}`;
  const desc = `LL-revoke-${salt}`;

  // 1) 隊長 6007 開容量 1 的隊（不設職業條件 → 候選為全池）
  await loginAs(page, { discordId: 6007, name: 'P-Full-Leader', role: 'user' });
  await page.goto('/teams/new');
  await page.locator('select').selectOption({ label: 'E2E王滿' });
  await page.locator('input[type="datetime-local"]').fill(slotLocal);
  await page.locator('textarea').fill(desc);
  await page.locator('label').filter({ hasText: '夜使者' }).click(); // 需至少一條職業需求列，候選才會出現（攻擊下限預設 0）
  await page.getByRole('button', { name: '開隊', exact: true }).click();
  await page.waitForURL(u => new URL(u).pathname === '/me/led-teams');

  // 2) 進候選 → 邀 A、B 兩人
  const hubCard = page.locator('li').filter({ hasText: desc });
  await hubCard.getByRole('link', { name: /挑候選/ }).click();
  await page.waitForURL(/\/teams\/\d+\/candidates/);
  for (const name of ['C-Full-A', 'C-Full-B']) {
    const row = page.locator('li').filter({ hasText: name });
    await expect(row).toBeVisible();
    await Promise.all([
      page.waitForResponse(r => r.url().includes('/Invitations') && r.request().method() === 'POST' && r.ok()),
      row.getByRole('button', { name: '邀請' }).click(),
    ]);
  }

  // 3) 先確認 B（6006）確實收到邀請（證明後面消失是被撤銷、非從未邀）
  await loginAs(page, { discordId: 6006, name: 'P-Full-B', role: 'user' });
  await page.goto('/me/teams');
  await expect(page.getByText('E2E王滿')).toBeVisible();

  // 4) A（6005）接受 → 隊伍額滿（容量 1）
  await loginAs(page, { discordId: 6005, name: 'P-Full-A', role: 'user' });
  await page.goto('/me/teams');
  await Promise.all([
    page.waitForResponse(r => /\/Invitations\/\d+$/.test(new URL(r.url()).pathname) && r.request().method() === 'PUT' && r.ok()),
    page.getByRole('button', { name: '接受' }).first().click(),
  ]);
  await expect(page.getByText('已加入')).toBeVisible();

  // 5) B 重看 → 邀請已被自動撤銷（E2E王滿 不再出現）
  await loginAs(page, { discordId: 6006, name: 'P-Full-B', role: 'user' });
  await page.goto('/me/teams');
  await expect(page.getByText('E2E王滿')).toHaveCount(0);
});
