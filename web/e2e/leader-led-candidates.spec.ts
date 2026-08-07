import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// leader-led Pull 流程（挑候選）：隊長開隊限定「英雄」→ 候選清單看到符合的玩家 → 邀請 → 玩家接受入隊。
// 覆蓋頁：/teams/new、/me/led-teams、/teams/[id]/candidates（邀請）、/me/teams（待處理邀請→接受）。
// 隔離：隊長 6004（test-login 自建）+ 候選 P-Cand(6003, 角色 c6003「英雄」, 全週可用, seed)。
// 冪等：唯一 slot + desc；候選全週可用故任一 slot 都命中（免吃星期/時區）。
// 前置：compose.e2e backend（非 Production）+ db/seed-e2e.sql。
test('隊長開隊 → 挑候選邀請 → 玩家接受入隊（Pull）', async ({ page }) => {
  test.slow();
  page.on('dialog', d => d.accept());

  const salt = Date.now();
  const pad = (n: number) => String(n).padStart(2, '0');
  const slot = new Date();
  slot.setDate(slot.getDate() + 10 + (salt % 7));                 // today+10 ~ +16（期內）
  const slotLocal = `${slot.getFullYear()}-${pad(slot.getMonth() + 1)}-${pad(slot.getDate())}T${pad(salt % 24)}:${pad(salt % 60)}`;
  const desc = `LL-pull-${salt}`;

  // ── 1) 隊長開隊，條件限定「英雄」1 位 ──
  await loginAs(page, { discordId: 6004, name: 'P-Cand-Leader', role: 'user' });
  await page.goto('/teams/new');
  await page.locator('select').selectOption({ label: 'E2E王' });
  await page.locator('input[type="datetime-local"]').fill(slotLocal);
  await page.locator('textarea').fill(desc);
  await page.locator('label').filter({ hasText: '英雄' }).click(); // 勾「英雄」職業（攻擊下限預設 0）
  await page.getByRole('button', { name: '開隊', exact: true }).click();
  await page.waitForURL(u => new URL(u).pathname === '/me/led-teams');

  // ── 2) 進候選清單 → 看到 C-Cand → 邀請 ──
  const hubCard = page.locator('li').filter({ hasText: desc });
  await expect(hubCard).toBeVisible();
  await hubCard.getByRole('link', { name: /挑候選/ }).click();
  await page.waitForURL(/\/teams\/\d+\/candidates/);
  const candRow = page.locator('li').filter({ hasText: 'C-Cand' });
  await expect(candRow).toBeVisible();
  await Promise.all([
    page.waitForResponse(r => r.url().includes('/Invitations') && r.request().method() === 'POST' && r.ok()),
    candRow.getByRole('button', { name: '邀請' }).click(),
  ]);
  await expect(candRow.getByRole('button', { name: '已邀請' })).toBeVisible();

  // ── 3) 候選玩家接受邀請 → 已加入 ──
  await loginAs(page, { discordId: 6003, name: 'P-Cand', role: 'user' });
  await page.goto('/me/teams');
  // Tier 2：邀請卡顯示隊伍人數（後端 confirmedCount/requireMembers）——此隊 0 confirmed、容量 6
  await expect(page.getByText('0/6')).toBeVisible();
  await Promise.all([
    page.waitForResponse(r => /\/Invitations\/\d+$/.test(new URL(r.url()).pathname) && r.request().method() === 'PUT' && r.ok()),
    page.getByRole('button', { name: '接受' }).first().click(),
  ]);
  await expect(page.getByText('已加入')).toBeVisible();
  await expect(page.getByText('E2E王').first()).toBeVisible();
});
