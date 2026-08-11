import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// period-less §8 Phase 3 即時車道：玩家發找隊(LfgIntent) → 隊長開即時團(Kind=Instant) → 候選來自看板 → 邀請 → 接受入隊。
// 覆蓋：/teams/instant(發布+看板)、/teams/new(即時 toggle)、/teams/[id]/candidates(即時候選)、/me/teams(接受)。
// 隔離：P-Lfg(8101,c8101 夜使者,seed) + 隊長 8102(test-login 自建)。
test('發找隊 → 開即時團 → 候選來自看板 → 邀請 → 接受入隊', async ({ page }) => {
  test.slow();
  page.on('dialog', d => d.accept());

  // 1) P-Lfg 發找隊（角色 c8101 + E2E王）
  await loginAs(page, { discordId: 8101, name: 'P-Lfg', role: 'user' });
  await page.goto('/teams/instant');
  await page.locator('select').first().selectOption('c8101');           // 角色
  await page.locator('select').nth(1).selectOption({ label: 'E2E王' }); // 想打的王
  await Promise.all([
    page.waitForResponse(r => r.url().includes('/LfgIntent') && r.request().method() === 'POST' && r.ok()),
    page.getByRole('button', { name: '我要找隊' }).click(),
  ]);
  // 看板出現自己（可取消）
  await expect(page.locator('li').filter({ hasText: 'C-Lfg' })).toBeVisible();

  // 2) 隊長 8102 開即時團（Kind=Instant）
  await loginAs(page, { discordId: 8102, name: 'P-Lfg-Leader', role: 'user' });
  await page.goto('/teams/new');
  await page.getByRole('button', { name: /即時團/ }).click();
  await page.locator('select').first().selectOption({ label: 'E2E王' });
  await page.locator('label').filter({ hasText: '夜使者' }).click(); // 需求職業 夜使者
  await page.getByRole('button', { name: '開隊', exact: true }).click();
  await page.waitForURL(u => new URL(u).pathname === '/me/led-teams');

  // 3) 挑候選 → 看板上的 C-Lfg 出現（即時候選）→ 邀請
  const hub = page.locator('li').filter({ hasText: 'E2E王' }).first();
  await hub.getByRole('link', { name: /挑候選/ }).click();
  await page.waitForURL(/\/teams\/\d+\/candidates/);
  const candRow = page.locator('li').filter({ hasText: 'C-Lfg' });
  await expect(candRow).toBeVisible();
  await Promise.all([
    page.waitForResponse(r => r.url().includes('/Invitations') && r.request().method() === 'POST' && r.ok()),
    candRow.getByRole('button', { name: '邀請' }).click(),
  ]);

  // 4) P-Lfg 接受邀請 → 已加入
  await loginAs(page, { discordId: 8101, name: 'P-Lfg', role: 'user' });
  await page.goto('/me/teams');
  await Promise.all([
    page.waitForResponse(r => /\/Invitations\/\d+$/.test(new URL(r.url()).pathname) && r.request().method() === 'PUT' && r.ok()),
    page.getByRole('button', { name: '接受' }).first().click(),
  ]);
  await expect(page.getByText('已加入')).toBeVisible();

  // 5) 入隊後找隊意圖已清除 → 看板不再有 C-Lfg
  await page.goto('/teams/instant');
  await expect(page.getByText('C-Lfg')).toHaveCount(0);
});
